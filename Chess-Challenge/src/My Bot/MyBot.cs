using ChessChallenge.API;
using System;
using System.Collections.Generic;
using System.Linq;

public class MyBot : IChessBot
{
    // Piece values: null, pawn, knight, bishop, rook, queen, king
    int[] pieceValues = { 0, 100, 300, 330, 500, 900, 10000 };

    int m_depth = 3;
    int maxMillisPerTurn;

    public Move Think(Board board, Timer timer)
    {
        int pieces = BitboardHelper.GetNumberOfSetBits(board.AllPiecesBitboard);
        if (pieces < 15)
        {
            m_depth = 4;
        }
        else if (pieces < 10)
        {
            m_depth = 5;
        }
        else if (pieces < 5)
        {
            m_depth = 8;
        }
        maxMillisPerTurn = timer.MillisecondsRemaining / 10;
        IOrderedEnumerable<Move> moves = board.GetLegalMoves().OrderByDescending(m => pieceValues[(int)m.MovePieceType]);
        List<Move> bestMoves = new List<Move>(moves);
        int bestScore = -100_000;
        foreach (Move move in moves) {
            board.MakeMove(move);
            int score = CalcRecursive(board, timer, m_depth, 1);
            board.UndoMove(move);
            if (score > bestScore)
            {
                bestMoves.Clear();
                bestScore = score;
                bestMoves.Add(move);
            }
            else if (score == bestScore)
            {
                bestMoves.Add(move);
            }
        }
        return SelectBestMove(board, bestMoves);
    }

    int EvalMaterial(Board board)
    {
        return board.GetAllPieceLists().Chunk(6).Select(chunk => chunk.Sum(list => list.Sum(p => pieceValues[(int)p.PieceType]))).Aggregate((white, black) => board.IsWhiteToMove ? white - black : black - white);
    }

    int CalcRecursive(Board board, Timer timer, int maxDepth, int currentDepth)
    {
        if (board.IsInCheckmate())
        {
            return currentDepth % 2 == 0 ? -200_000 : 200_000;
        }
        if (board.IsInsufficientMaterial() || board.IsRepeatedPosition() || board.FiftyMoveCounter == 50)
        {
            return 0;
        }
        if (currentDepth >= maxDepth)
        {
            return currentDepth % 2 == 0 ? EvalMaterial(board) : -EvalMaterial(board);
        }
        int bestScore = -100_000_000;
        foreach (Move move in board.GetLegalMoves().OrderByDescending(m => pieceValues[(int)m.MovePieceType]))
        {
            board.MakeMove(move);
            int score = CalcRecursive(board, timer, maxDepth, currentDepth + 1);
            board.UndoMove(move);
            if (score > bestScore)
            {
                bestScore = score;
            }
            if (timer.MillisecondsElapsedThisTurn > maxMillisPerTurn)
            {
                return bestScore;
            }
        }
        return bestScore;
    }

    Move SelectBestMove(Board board, List<Move> moves)
    {
        Move bestMove = moves[new Random().Next(moves.Count)];
        int bestValue = -100_000;

        foreach (Move move in moves)
        {
            Square kingSquare = board.GetKingSquare(!board.IsWhiteToMove);
            int promotionValue = pieceValues[(int)move.CapturePieceType];
            int captureValue = pieceValues[(int)move.CapturePieceType];
            int lostPieceValue = HighestLostPiece(board, move);
            int progress = move.MovePieceType == PieceType.Pawn ? GetDistance(move.StartSquare, move.TargetSquare) : GetDistance(kingSquare, move.StartSquare) - GetDistance(kingSquare, move.TargetSquare);

            int score = captureValue - lostPieceValue + promotionValue + progress;
            if (score > bestValue)
            {
                bestMove = move;
                bestValue = score;
            }
        }

        return bestMove;
    }

    int HighestLostPiece(Board board, Move move) {
        board.MakeMove(move);
        IEnumerable<Square> attackedSquares = board.GetLegalMoves(true).Select(m => m.TargetSquare);
        List<Piece> myPieces = board.GetAllPieceLists().Chunk(6).ElementAt(board.IsWhiteToMove ? 1 : 0).SelectMany(list => list.ToList()).ToList();
        int maxValue = myPieces.Where(p => attackedSquares.Contains(p.Square)).Where(p => !board.SquareIsAttackedByOpponent(p.Square)).Select(p => pieceValues[(int)p.PieceType]).DefaultIfEmpty(0).Max();
        board.UndoMove(move);
        return maxValue;
    }

    int GetDistance(Square square1, Square square2)
    {
        return Math.Abs(square1.File - square2.File) + Math.Abs(square1.Rank - square2.Rank);
    }
}