using ChessChallenge.API;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

public class MyBot : IChessBot
{
    // Piece values: null, pawn, knight, bishop, rook, queen, king
    int[] pieceValues = { 0, 100, 300, 330, 500, 900, 10000 };
    readonly int maxPieceSum;

    int m_depth = 3;
    int maxMillisPerTurn;

    public MyBot()
    {
        maxPieceSum = pieceValues[1] * 8 + pieceValues[2] * 2 + pieceValues[3] * 2 + pieceValues[4] * 2 + pieceValues[5] + pieceValues[6];
    }

    public Move Think(Board board, Timer timer)
    {
        int pieces = BitboardHelper.GetNumberOfSetBits(board.AllPiecesBitboard);
        if (pieces < 15) m_depth = 4;
        else if (pieces <= 10) m_depth = 5;
        else if (pieces <= 8) m_depth = 6;
        else if (pieces <= 6) m_depth = 8;

        maxMillisPerTurn = timer.MillisecondsRemaining / 10;

        List<Move> bestMoves = new List<Move>();
        int bestScore = int.MinValue;
        foreach (Move move in board.GetLegalMoves().OrderByDescending(m => pieceValues[(int)m.MovePieceType])) {
            board.MakeMove(move);
            int score = CalcRecursive(board, timer, m_depth, 1);
            board.UndoMove(move);

            if (score > bestScore)
            {
                bestMoves.Clear();
                bestScore = score;
            }
            if (score >= bestScore) bestMoves.Add(move);
        }
        return SelectBestMove(board, bestMoves);
    }

    int EvalMaterial(Board board)
    {
        return board.GetAllPieceLists().Chunk(6).Select(chunk => chunk.Sum(list => list.Sum(p => pieceValues[(int)p.PieceType]))).Aggregate((white, black) => white - black);
    }

    int CalcRecursive(Board board, Timer timer, int maxDepth, int currentDepth)
    {
        if (board.IsInCheckmate()) return currentDepth % 2 == 0 ? -maxPieceSum : maxPieceSum;
        if (board.IsInsufficientMaterial() || board.IsRepeatedPosition() || board.FiftyMoveCounter == 50 || board.IsDraw()) return 0;

        int boardScore = (currentDepth % 2 == 0) ^ board.IsWhiteToMove ? -EvalMaterial(board) : EvalMaterial(board);
        if (currentDepth >= maxDepth) return boardScore;

        int bestScore = int.MinValue;
        foreach (Move move in board.GetLegalMoves().OrderByDescending(m => pieceValues[(int)m.MovePieceType]))
        {
            board.MakeMove(move);
            int score = (int) (CalcRecursive(board, timer, maxDepth, currentDepth + 1) * 0.9 + boardScore);
            board.UndoMove(move);
            if (score > bestScore) bestScore = score;
            if (timer.MillisecondsElapsedThisTurn > maxMillisPerTurn) return bestScore;
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
        int maxValue = board.GetAllPieceLists().Chunk(6).ElementAt(board.IsWhiteToMove ? 1 : 0).SelectMany(list => list)
            .Where(p => board.GetLegalMoves(true).Select(m => m.TargetSquare).Contains(p.Square)).Where(p => !board.SquareIsAttackedByOpponent(p.Square)).Select(p => pieceValues[(int)p.PieceType]).DefaultIfEmpty(0).Max();
        board.UndoMove(move);
        return maxValue;
    }

    int GetDistance(Square square1, Square square2)
    {
        return Math.Abs(square1.File - square2.File) + Math.Abs(square1.Rank - square2.Rank);
    }
}